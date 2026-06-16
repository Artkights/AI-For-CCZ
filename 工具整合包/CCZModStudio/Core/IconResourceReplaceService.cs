using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class IconResourceReplaceService
{
    private const int RtBitmap = 2;
    private const ushort NeutralLanguage = 0;
    private readonly WriteOperationReportService _reportService = new();

    public IconResourceReplacePreviewResult PreviewReplaceBitmapIcon(CczProject project, string targetPath, int iconIndex, string sourcePath)
    {
        var target = Path.GetFullPath(targetPath);
        var source = Path.GetFullPath(sourcePath);
        EnsureTargetInsideProject(project, target);
        if (!File.Exists(target)) throw new FileNotFoundException("目标 DLL 文件不存在。", target);
        if (!File.Exists(source)) throw new FileNotFoundException("来源图片文件不存在。", source);

        var resources = ParseBitmapResources(target);
        var pair = ResolveBitmapResourcePair(resources, iconIndex);
        var sourceInfo = ReadSourceImageInfo(source);
        var oldBytes = File.ReadAllBytes(target);
        var sourceBytes = File.ReadAllBytes(source);
        var warnings = BuildReplaceWarnings(pair, sourceInfo);
        return new IconResourceReplacePreviewResult
        {
            TargetPath = target,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, target),
            IconIndex = iconIndex,
            ResourceIds = pair.Select(x => x.Id).ToArray(),
            SourcePath = source,
            OperationKind = "替换RT_BITMAP图标",
            OldFileSizeBytes = oldBytes.LongLength,
            SourceSizeBytes = sourceBytes.LongLength,
            OldFileSha256 = WriteOperationReportService.ComputeSha256(oldBytes),
            SourceSha256 = WriteOperationReportService.ComputeSha256(sourceBytes),
            SourceWidth = sourceInfo.Width,
            SourceHeight = sourceInfo.Height,
            ResourceFormat = "DLL RT_BITMAP",
            FormatWarnings = warnings,
            RiskSummary = BuildReplaceRiskSummary(pair, warnings)
        };
    }

    public IconResourceReplaceResult ReplaceBitmapIcon(CczProject project, string targetPath, int iconIndex, string sourcePath)
    {
        var preview = PreviewReplaceBitmapIcon(project, targetPath, iconIndex, sourcePath);
        var resources = ParseBitmapResources(preview.TargetPath);
        var pair = ResolveBitmapResourcePair(resources, iconIndex);
        var updates = BuildBitmapUpdatesFromImage(sourcePath, pair);
        return ApplyBitmapUpdates(project, preview, updates);
    }

    public IconResourceBatchReplacePreviewResult PreviewReplaceBitmapIcons(
        CczProject project,
        string targetPath,
        IReadOnlyList<IconResourceBatchReplaceRequest> requests)
    {
        if (requests.Count == 0)
        {
            throw new InvalidOperationException("没有可导入的 DLL 图标。");
        }

        var target = Path.GetFullPath(targetPath);
        EnsureTargetInsideProject(project, target);
        if (!File.Exists(target)) throw new FileNotFoundException("目标 DLL 文件不存在。", target);

        var duplicateIcon = requests
            .GroupBy(request => request.IconIndex)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateIcon != null)
        {
            throw new InvalidOperationException($"批量导入包含重复图标编号：{duplicateIcon.Key}。");
        }

        var resources = ParseBitmapResources(target);
        var oldBytes = File.ReadAllBytes(target);
        var items = new List<IconResourceBatchReplacePreviewItem>();
        var warnings = new List<string>();
        foreach (var request in requests.OrderBy(request => request.IconIndex))
        {
            var source = Path.GetFullPath(request.SourcePath);
            if (!File.Exists(source)) throw new FileNotFoundException("来源图片文件不存在。", source);

            var pair = ResolveBitmapResourcePair(resources, request.IconIndex);
            var sourceInfo = ReadSourceImageInfo(source);
            var sourceBytes = File.ReadAllBytes(source);
            var itemWarnings = BuildReplaceWarnings(pair, sourceInfo);
            warnings.AddRange(itemWarnings.Select(warning => $"图标#{request.IconIndex}：{warning}"));
            items.Add(new IconResourceBatchReplacePreviewItem
            {
                IconIndex = request.IconIndex,
                ResourceIds = pair.Select(x => x.Id).ToArray(),
                SourcePath = source,
                SourceLabel = string.IsNullOrWhiteSpace(request.SourceLabel) ? source : request.SourceLabel,
                SourceSizeBytes = sourceBytes.LongLength,
                SourceSha256 = WriteOperationReportService.ComputeSha256(sourceBytes),
                SourceWidth = sourceInfo.Width,
                SourceHeight = sourceInfo.Height,
                FormatWarnings = itemWarnings
            });
        }

        return new IconResourceBatchReplacePreviewResult
        {
            TargetPath = target,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, target),
            Requests = requests.ToArray(),
            Items = items,
            OperationKind = requests.Select(request => request.OperationKind).FirstOrDefault(kind => !string.IsNullOrWhiteSpace(kind)) ?? "批量替换RT_BITMAP图标",
            OldFileSizeBytes = oldBytes.LongLength,
            OldFileSha256 = WriteOperationReportService.ComputeSha256(oldBytes),
            ResourceFormat = "DLL RT_BITMAP",
            FormatWarnings = warnings,
            RiskSummary = BuildBatchReplaceRiskSummary(items, warnings)
        };
    }

    public IconResourceBatchReplaceResult ReplaceBitmapIcons(
        CczProject project,
        string targetPath,
        IReadOnlyList<IconResourceBatchReplaceRequest> requests)
    {
        var preview = PreviewReplaceBitmapIcons(project, targetPath, requests);
        var resources = ParseBitmapResources(preview.TargetPath);
        var updates = new List<BitmapResourceUpdate>();
        foreach (var request in requests.OrderBy(request => request.IconIndex))
        {
            var pair = ResolveBitmapResourcePair(resources, request.IconIndex);
            updates.AddRange(BuildBitmapUpdatesFromImage(request.SourcePath, pair));
        }

        return ApplyBatchBitmapUpdates(project, preview, updates);
    }

    public IconResourceReplacePreviewResult PreviewClearBitmapIcon(CczProject project, string targetPath, int iconIndex)
    {
        var target = Path.GetFullPath(targetPath);
        EnsureTargetInsideProject(project, target);
        if (!File.Exists(target)) throw new FileNotFoundException("目标 DLL 文件不存在。", target);

        var resources = ParseBitmapResources(target);
        var pair = ResolveBitmapResourcePair(resources, iconIndex);
        var oldBytes = File.ReadAllBytes(target);
        return new IconResourceReplacePreviewResult
        {
            TargetPath = target,
            TargetRelativePath = WriteOperationReportService.ToProjectRelativePath(project, target),
            IconIndex = iconIndex,
            ResourceIds = pair.Select(x => x.Id).ToArray(),
            SourcePath = "<透明占位>",
            OperationKind = "清空RT_BITMAP图标",
            OldFileSizeBytes = oldBytes.LongLength,
            SourceSizeBytes = 0,
            OldFileSha256 = WriteOperationReportService.ComputeSha256(oldBytes),
            SourceSha256 = string.Empty,
            ResourceFormat = "DLL RT_BITMAP",
            FormatWarnings = Array.Empty<string>(),
            RiskSummary = "清空不会删除资源ID，而是按原尺寸写入透明位图，避免破坏字段编号到资源ID的对应关系。"
        };
    }

    public IconResourceReplaceResult ClearBitmapIcon(CczProject project, string targetPath, int iconIndex)
    {
        var preview = PreviewClearBitmapIcon(project, targetPath, iconIndex);
        var resources = ParseBitmapResources(preview.TargetPath);
        var pair = ResolveBitmapResourcePair(resources, iconIndex);
        var updates = pair.Select(resource => new BitmapResourceUpdate(resource.Id, BuildTransparentDib(resource.Width, resource.Height))).ToArray();
        return ApplyBitmapUpdates(project, preview, updates);
    }

    private IconResourceReplaceResult ApplyBitmapUpdates(
        CczProject project,
        IconResourceReplacePreviewResult preview,
        IReadOnlyList<BitmapResourceUpdate> updates)
    {
        if (updates.Count == 0)
        {
            throw new InvalidOperationException("没有可写入的 DLL 图标资源。");
        }

        var backupPath = CreateBeforeSaveBackup(project, preview.TargetPath);
        var beforeBytes = File.ReadAllBytes(preview.TargetPath);
        var updateHandle = BeginUpdateResource(preview.TargetPath, false);
        if (updateHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("BeginUpdateResource 失败，Win32Error=" + Marshal.GetLastWin32Error());
        }

        var committed = false;
        try
        {
            foreach (var update in updates)
            {
                if (!UpdateResource(updateHandle, (IntPtr)RtBitmap, (IntPtr)update.ResourceId, NeutralLanguage, update.DibBytes, update.DibBytes.Length))
                {
                    throw new InvalidOperationException($"UpdateResource RT_BITMAP ID={update.ResourceId} 失败，Win32Error={Marshal.GetLastWin32Error()}");
                }
            }

            if (!EndUpdateResource(updateHandle, false))
            {
                throw new InvalidOperationException("EndUpdateResource 失败，Win32Error=" + Marshal.GetLastWin32Error());
            }

            committed = true;
        }
        finally
        {
            if (!committed)
            {
                EndUpdateResource(updateHandle, true);
            }
        }

        var afterBytes = File.ReadAllBytes(preview.TargetPath);
        var changedBytes = EstimateChangedBytes(beforeBytes, afterBytes);
        var newHash = WriteOperationReportService.ComputeSha256(afterBytes);
        var reportPath = WriteTextReport(project, preview, backupPath, afterBytes.LongLength, changedBytes, newHash);
        var reportJsonPath = WriteStructuredReport(project, preview, backupPath, reportPath, afterBytes.LongLength, changedBytes, newHash);

        return new IconResourceReplaceResult
        {
            TargetPath = preview.TargetPath,
            TargetRelativePath = preview.TargetRelativePath,
            IconIndex = preview.IconIndex,
            ResourceIds = preview.ResourceIds,
            SourcePath = preview.SourcePath,
            OperationKind = preview.OperationKind,
            OldFileSizeBytes = preview.OldFileSizeBytes,
            SourceSizeBytes = preview.SourceSizeBytes,
            OldFileSha256 = preview.OldFileSha256,
            SourceSha256 = preview.SourceSha256,
            SourceWidth = preview.SourceWidth,
            SourceHeight = preview.SourceHeight,
            ResourceFormat = preview.ResourceFormat,
            FormatWarnings = preview.FormatWarnings,
            RiskSummary = preview.RiskSummary,
            BackupPath = backupPath,
            ReportPath = reportPath,
            ReportJsonPath = reportJsonPath,
            NewFileSizeBytes = afterBytes.LongLength,
            ChangedBytesEstimate = changedBytes,
            NewFileSha256 = newHash
        };
    }

    private IconResourceBatchReplaceResult ApplyBatchBitmapUpdates(
        CczProject project,
        IconResourceBatchReplacePreviewResult preview,
        IReadOnlyList<BitmapResourceUpdate> updates)
    {
        if (updates.Count == 0)
        {
            throw new InvalidOperationException("没有可写入的 DLL 图标资源。");
        }

        var backupPath = CreateBeforeSaveBackup(project, preview.TargetPath);
        var beforeBytes = File.ReadAllBytes(preview.TargetPath);
        var updateHandle = BeginUpdateResource(preview.TargetPath, false);
        if (updateHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("BeginUpdateResource 失败，Win32Error=" + Marshal.GetLastWin32Error());
        }

        var committed = false;
        try
        {
            foreach (var update in updates)
            {
                if (!UpdateResource(updateHandle, (IntPtr)RtBitmap, (IntPtr)update.ResourceId, NeutralLanguage, update.DibBytes, update.DibBytes.Length))
                {
                    throw new InvalidOperationException($"UpdateResource RT_BITMAP ID={update.ResourceId} 失败，Win32Error={Marshal.GetLastWin32Error()}");
                }
            }

            if (!EndUpdateResource(updateHandle, false))
            {
                throw new InvalidOperationException("EndUpdateResource 失败，Win32Error=" + Marshal.GetLastWin32Error());
            }

            committed = true;
        }
        finally
        {
            if (!committed)
            {
                EndUpdateResource(updateHandle, true);
            }
        }

        var afterBytes = File.ReadAllBytes(preview.TargetPath);
        var changedBytes = EstimateChangedBytes(beforeBytes, afterBytes);
        var newHash = WriteOperationReportService.ComputeSha256(afterBytes);
        var reportPath = WriteBatchTextReport(project, preview, backupPath, afterBytes.LongLength, changedBytes, newHash);
        var reportJsonPath = WriteBatchStructuredReport(project, preview, backupPath, reportPath, afterBytes.LongLength, changedBytes, newHash);

        return new IconResourceBatchReplaceResult
        {
            TargetPath = preview.TargetPath,
            TargetRelativePath = preview.TargetRelativePath,
            Requests = preview.Requests,
            Items = preview.Items,
            OperationKind = preview.OperationKind,
            OldFileSizeBytes = preview.OldFileSizeBytes,
            OldFileSha256 = preview.OldFileSha256,
            ResourceFormat = preview.ResourceFormat,
            FormatWarnings = preview.FormatWarnings,
            RiskSummary = preview.RiskSummary,
            BackupPath = backupPath,
            ReportPath = reportPath,
            ReportJsonPath = reportJsonPath,
            NewFileSizeBytes = afterBytes.LongLength,
            ChangedBytesEstimate = changedBytes,
            NewFileSha256 = newHash
        };
    }

    private static IReadOnlyList<BitmapResourceUpdate> BuildBitmapUpdatesFromImage(string sourcePath, IReadOnlyList<BitmapResourceRecord> targets)
        => targets
            .Select(target => new BitmapResourceUpdate(target.Id, BuildDibForTargetSize(sourcePath, target.Width, target.Height)))
            .ToArray();

    private static SourceImageInfo ReadSourceImageInfo(string sourcePath)
    {
        try
        {
            using var image = Image.FromFile(sourcePath);
            return new SourceImageInfo(image.Width, image.Height);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException)
        {
            throw new InvalidOperationException("来源文件不是可解码图片，DLL 图标替换仅支持 BMP/JPG/PNG 等 GDI+ 可读取图片。", ex);
        }
    }

    private static byte[] BuildDibForTargetSize(string sourcePath, int targetWidth, int targetHeight)
    {
        using var source = Image.FromFile(sourcePath);
        using var bitmap = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            var scale = Math.Min(targetWidth / (float)source.Width, targetHeight / (float)source.Height);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) scale = 1;
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));
            var x = (targetWidth - width) / 2;
            var y = (targetHeight - height) / 2;
            graphics.DrawImage(source, new Rectangle(x, y, width, height));
        }

        return BitmapToDib(bitmap);
    }

    private static byte[] BuildTransparentDib(int width, int height)
    {
        using var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
        }

        return BitmapToDib(bitmap);
    }

    private static byte[] BitmapToDib(Bitmap bitmap)
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

        // CCZ 6.5 reads these RT_BITMAP resources as top-first scanlines even though the
        // legacy resources keep a positive DIB height. Writing rows in BMP bottom-up order
        // makes imported strategy icons appear upside down in game.
        var offset = 40;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                dib[offset++] = color.B;
                dib[offset++] = color.G;
                dib[offset++] = color.R;
                dib[offset++] = color.A;
            }
        }

        return dib;
    }

    private static IReadOnlyList<string> BuildReplaceWarnings(IReadOnlyList<BitmapResourceRecord> pair, SourceImageInfo source)
    {
        var warnings = new List<string>();
        if (pair.Count == 0)
        {
            warnings.Add("没有解析到目标 RT_BITMAP 资源。");
            return warnings;
        }

        var maxWidth = pair.Max(x => x.Width);
        var maxHeight = pair.Max(x => x.Height);
        if (source.Width != maxWidth || source.Height != maxHeight)
        {
            warnings.Add($"来源图片尺寸 {source.Width}x{source.Height} 会缩放/居中到 DLL 资源尺寸 {string.Join("/", pair.Select(x => $"{x.Width}x{x.Height}"))}。");
        }

        if (pair.Count == 1)
        {
            warnings.Add("只找到一个 RT_BITMAP 资源；若该 DLL 原本按 16x16/32x32 成对保存，另一个尺寸可能缺失。");
        }

        return warnings;
    }

    private static string BuildReplaceRiskSummary(IReadOnlyList<BitmapResourceRecord> pair, IReadOnlyList<string> warnings)
    {
        var ids = pair.Count == 0 ? "无" : string.Join(", ", pair.Select(x => x.Id.ToString(CultureInfo.InvariantCulture)));
        var baseText = $"按字段编号定位 RT_BITMAP 资源 ID={ids} 并替换其 DIB 数据；写入前自动备份 DLL。";
        return warnings.Count == 0 ? baseText : baseText + "提示：" + string.Join("；", warnings);
    }

    private static string BuildBatchReplaceRiskSummary(IReadOnlyList<IconResourceBatchReplacePreviewItem> items, IReadOnlyList<string> warnings)
    {
        var icons = items.Count == 0
            ? "无"
            : string.Join(", ", items.Select(item => $"#{item.IconIndex}=ID{string.Join("/", item.ResourceIds)}"));
        var baseText = $"批量按字段编号定位 RT_BITMAP 资源并替换 DIB 数据：{icons}；写入前只备份一次 DLL。";
        return warnings.Count == 0 ? baseText : baseText + "提示：" + string.Join("；", warnings);
    }

    private static IReadOnlyList<BitmapResourceRecord> ResolveBitmapResourcePair(IReadOnlyList<BitmapResourceRecord> resources, int iconIndex)
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
                .OrderBy(x => x.Width * x.Height)
                .ToArray();
            if (pair.Length == 0)
            {
                throw new InvalidOperationException($"图标编号 {iconIndex} 没有匹配 RT_BITMAP ID={smallId}/{largeId}。");
            }

            return pair;
        }

        if (iconIndex >= resources.Count)
        {
            throw new InvalidOperationException($"图标编号 {iconIndex} 超出 DLL 位图资源范围 0-{resources.Count - 1}。");
        }

        return new[] { resources.OrderBy(x => x.Id).ElementAt(iconIndex) };
    }

    private static IReadOnlyList<BitmapResourceRecord> ParseBitmapResources(string sourcePath)
    {
        try
        {
            var data = File.ReadAllBytes(sourcePath);
            if (data.Length < 0x40 || data[0] != 'M' || data[1] != 'Z') return Array.Empty<BitmapResourceRecord>();
            var peOffset = BitConverter.ToInt32(data, 0x3C);
            if (peOffset <= 0 || peOffset + 248 >= data.Length) return Array.Empty<BitmapResourceRecord>();
            var sectionCount = BitConverter.ToUInt16(data, peOffset + 6);
            var optionalHeaderSize = BitConverter.ToUInt16(data, peOffset + 20);
            var optionalHeaderOffset = peOffset + 24;
            var magic = BitConverter.ToUInt16(data, optionalHeaderOffset);
            var dataDirectoryOffset = magic == 0x20B ? optionalHeaderOffset + 112 : optionalHeaderOffset + 96;
            if (dataDirectoryOffset + 2 * 8 + 8 > data.Length) return Array.Empty<BitmapResourceRecord>();
            var resourceRva = BitConverter.ToInt32(data, dataDirectoryOffset + 2 * 8);
            if (resourceRva <= 0) return Array.Empty<BitmapResourceRecord>();
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
            if (resourceBaseOffset < 0 || resourceBaseOffset + 16 > data.Length) return Array.Empty<BitmapResourceRecord>();
            var result = new List<BitmapResourceRecord>();
            ReadResourceDirectory(data, sections, resourceBaseOffset, resourceBaseOffset, 0, new List<int>(), result);
            return result
                .Where(x => x.Width > 0 && x.Height > 0)
                .OrderBy(x => x.Id)
                .ToArray();
        }
        catch
        {
            return Array.Empty<BitmapResourceRecord>();
        }
    }

    private static void ReadResourceDirectory(
        byte[] data,
        IReadOnlyList<PeSectionInfo> sections,
        int resourceBaseOffset,
        int directoryOffset,
        int level,
        List<int> path,
        List<BitmapResourceRecord> output)
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
            output.Add(new BitmapResourceRecord(path[1], width, height, size));
        }
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

        if (headerSize is not (40 or 108 or 124) || bytes.Length < 16) return false;
        width = BitConverter.ToInt32(bytes, 4);
        height = Math.Abs(BitConverter.ToInt32(bytes, 8));
        return width > 0 && height > 0;
    }

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

    private static void EnsureTargetInsideProject(CczProject project, string targetPath)
    {
        var gameRoot = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!targetPath.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标 DLL 文件不在当前项目目录内，禁止写入：" + targetPath);
        }
    }

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var safeRelative = MakeSafeRelativeName(project, filePath);
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{safeRelative}");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupRoot, $"{stamp}_{suffix++}_{safeRelative}");
        }

        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }

    private static string MakeSafeRelativeName(CczProject project, string filePath)
    {
        var relative = Path.GetRelativePath(project.GameRoot, filePath);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            relative = relative.Replace(invalid, '_');
        }

        return relative.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
    }

    private static int EstimateChangedBytes(byte[] oldBytes, byte[] newBytes)
    {
        long count = Math.Abs((long)oldBytes.Length - newBytes.Length);
        var common = Math.Min(oldBytes.Length, newBytes.Length);
        for (var i = 0; i < common; i++)
        {
            if (oldBytes[i] != newBytes[i]) count++;
        }

        return count > int.MaxValue ? int.MaxValue : (int)count;
    }

    private static string WriteTextReport(
        CczProject project,
        IconResourceReplacePreviewResult preview,
        string backupPath,
        long newSize,
        int changedBytes,
        string newHash)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_DllIconReplaceReport.txt");
        var lines = new[]
        {
            "CCZModStudio DLL Icon Replace Report",
            "CreatedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "GameRoot=" + project.GameRoot,
            "Target=" + preview.TargetPath,
            "TargetRelative=" + preview.TargetRelativePath,
            "OperationKind=" + preview.OperationKind,
            "IconIndex=" + preview.IconIndex.ToString(CultureInfo.InvariantCulture),
            "ResourceIds=" + string.Join(",", preview.ResourceIds),
            "Source=" + preview.SourcePath,
            "Backup=" + backupPath,
            "OldSize=" + preview.OldFileSizeBytes.ToString(CultureInfo.InvariantCulture),
            "NewSize=" + newSize.ToString(CultureInfo.InvariantCulture),
            "ChangedBytesEstimate=" + changedBytes.ToString(CultureInfo.InvariantCulture),
            "OldSHA256=" + preview.OldFileSha256,
            "NewSHA256=" + newHash,
            "SourceSHA256=" + preview.SourceSha256,
            "Warnings=" + (preview.FormatWarnings.Count == 0 ? "无" : string.Join(" | ", preview.FormatWarnings)),
            "RiskSummary=" + preview.RiskSummary,
            string.Empty,
            "说明：当前 DLL 图标写回针对曹操传样本中的 RT_BITMAP 成对资源；不重排资源 ID。"
        };
        File.WriteAllLines(reportPath, lines, Encoding.UTF8);
        return reportPath;
    }

    private static string WriteBatchTextReport(
        CczProject project,
        IconResourceBatchReplacePreviewResult preview,
        string backupPath,
        long newSize,
        int changedBytes,
        string newHash)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(backupRoot, $"{stamp}_DllIconBatchReplaceReport.txt");
        var lines = new List<string>
        {
            "CCZModStudio DLL Icon Batch Replace Report",
            "CreatedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "GameRoot=" + project.GameRoot,
            "Target=" + preview.TargetPath,
            "TargetRelative=" + preview.TargetRelativePath,
            "OperationKind=" + preview.OperationKind,
            "OperationCount=" + preview.Items.Count.ToString(CultureInfo.InvariantCulture),
            "Backup=" + backupPath,
            "OldSize=" + preview.OldFileSizeBytes.ToString(CultureInfo.InvariantCulture),
            "NewSize=" + newSize.ToString(CultureInfo.InvariantCulture),
            "ChangedBytesEstimate=" + changedBytes.ToString(CultureInfo.InvariantCulture),
            "OldSHA256=" + preview.OldFileSha256,
            "NewSHA256=" + newHash,
            "Warnings=" + (preview.FormatWarnings.Count == 0 ? "无" : string.Join(" | ", preview.FormatWarnings)),
            "RiskSummary=" + preview.RiskSummary,
            string.Empty,
            "Items:"
        };
        lines.AddRange(preview.Items.Select(item =>
            $"IconIndex={item.IconIndex}; ResourceIds={string.Join(",", item.ResourceIds)}; Source={item.SourcePath}; SourceSize={item.SourceWidth}x{item.SourceHeight}; SourceSHA256={item.SourceSha256}"));
        lines.Add(string.Empty);
        lines.Add("说明：当前 DLL 图标批量写回针对曹操传样本中的 RT_BITMAP 成对资源；不重排资源 ID。");
        File.WriteAllLines(reportPath, lines, Encoding.UTF8);
        return reportPath;
    }

    private string WriteStructuredReport(
        CczProject project,
        IconResourceReplacePreviewResult preview,
        string backupPath,
        string textReportPath,
        long newSize,
        int changedBytes,
        string newHash)
    {
        var report = new WriteOperationReport
        {
            OperationKind = "DLL图标RT_BITMAP替换",
            SourceAction = "DLL图标资源写入前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = preview.TargetRelativePath,
            TargetPath = preview.TargetPath,
            BackupPath = backupPath,
            TextReportPath = textReportPath,
            BeforeSha256 = preview.OldFileSha256,
            AfterSha256 = newHash,
            ChangedBytes = changedBytes,
            Summary = $"{preview.OperationKind}：{preview.TargetRelativePath} 编号 #{preview.IconIndex}，资源ID {string.Join(",", preview.ResourceIds)}。",
            SafetyNotes = "当前只写 DLL PE 资源目录中的 RT_BITMAP DIB 数据；不重排编号，不修改游戏数据表字段。",
            FormatCheckSummary = preview.ResourceFormat,
            RiskSummary = preview.RiskSummary,
            Changes =
            [
                new WriteOperationChange
                {
                    Category = "DLL图标",
                    TableName = preview.TargetRelativePath,
                    RowIndex = preview.IconIndex,
                    ColumnName = "RT_BITMAP",
                    OffsetHex = string.Join(",", preview.ResourceIds.Select(id => "ID=" + id.ToString(CultureInfo.InvariantCulture))),
                    ByteLength = newSize <= int.MaxValue ? (int)newSize : null,
                    OldValue = $"size={preview.OldFileSizeBytes}; sha256={preview.OldFileSha256}",
                    NewValue = $"size={newSize}; sha256={newHash}; source={preview.SourcePath}",
                    Annotation = preview.RiskSummary
                }
            ],
            Metadata =
            {
                ["IconIndex"] = preview.IconIndex.ToString(CultureInfo.InvariantCulture),
                ["ResourceIds"] = string.Join(",", preview.ResourceIds),
                ["OperationKind"] = preview.OperationKind,
                ["SourcePath"] = preview.SourcePath,
                ["SourceSha256"] = preview.SourceSha256,
                ["FormatWarnings"] = preview.FormatWarnings.Count == 0 ? "无" : string.Join("；", preview.FormatWarnings)
            }
        };

        return _reportService.WriteJsonReport(report, backupPath);
    }

    private string WriteBatchStructuredReport(
        CczProject project,
        IconResourceBatchReplacePreviewResult preview,
        string backupPath,
        string textReportPath,
        long newSize,
        int changedBytes,
        string newHash)
    {
        var report = new WriteOperationReport
        {
            OperationKind = "DLL图标RT_BITMAP批量替换",
            SourceAction = "DLL图标资源批量写入前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = preview.TargetRelativePath,
            TargetPath = preview.TargetPath,
            BackupPath = backupPath,
            TextReportPath = textReportPath,
            BeforeSha256 = preview.OldFileSha256,
            AfterSha256 = newHash,
            ChangedBytes = changedBytes,
            Summary = $"{preview.OperationKind}：{preview.TargetRelativePath}，共 {preview.Items.Count} 个图标。",
            SafetyNotes = "当前只写 DLL PE 资源目录中的 RT_BITMAP DIB 数据；不重排编号，不修改游戏数据表字段。",
            FormatCheckSummary = preview.ResourceFormat,
            RiskSummary = preview.RiskSummary,
            Changes = preview.Items.Select(item => new WriteOperationChange
            {
                Category = "DLL图标",
                TableName = preview.TargetRelativePath,
                RowIndex = item.IconIndex,
                ColumnName = "RT_BITMAP",
                OffsetHex = string.Join(",", item.ResourceIds.Select(id => "ID=" + id.ToString(CultureInfo.InvariantCulture))),
                ByteLength = newSize <= int.MaxValue ? (int)newSize : null,
                OldValue = $"size={preview.OldFileSizeBytes}; sha256={preview.OldFileSha256}",
                NewValue = $"size={newSize}; sha256={newHash}; source={item.SourcePath}; sourceSha256={item.SourceSha256}",
                Annotation = item.FormatWarnings.Count == 0 ? "按批量导入请求替换。 " : string.Join("；", item.FormatWarnings)
            }).ToList(),
            Metadata =
            {
                ["OperationKind"] = preview.OperationKind,
                ["OperationCount"] = preview.Items.Count.ToString(CultureInfo.InvariantCulture),
                ["IconIndexes"] = string.Join(",", preview.Items.Select(item => item.IconIndex.ToString(CultureInfo.InvariantCulture))),
                ["FormatWarnings"] = preview.FormatWarnings.Count == 0 ? "无" : string.Join("；", preview.FormatWarnings)
            }
        };

        return _reportService.WriteJsonReport(report, backupPath);
    }

    [DllImport("kernel32.dll", EntryPoint = "BeginUpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResource(string pFileName, [MarshalAs(UnmanagedType.Bool)] bool bDeleteExistingResources);

    [DllImport("kernel32.dll", EntryPoint = "UpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, int cbData);

    [DllImport("kernel32.dll", EntryPoint = "EndUpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndUpdateResource(IntPtr hUpdate, [MarshalAs(UnmanagedType.Bool)] bool fDiscard);

    private sealed record BitmapResourceRecord(int Id, int Width, int Height, int SizeBytes);
    private sealed record BitmapResourceUpdate(int ResourceId, byte[] DibBytes);
    private sealed record PeSectionInfo(int VirtualAddress, int Size, int RawPointer);
    private sealed record SourceImageInfo(int Width, int Height);
}
